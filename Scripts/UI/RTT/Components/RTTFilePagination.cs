using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// RTTFilePagination - Pagination bar for File Manager.
/// Styled like RTTTaskbarExpansion: Floating glass panel below the main content.
/// </summary>
public class RTTFilePagination : RTTCanvasBase
{
    #region Configuration
    [SerializeField] private float frameWidth = 400f; // Will be overwritten by code
    [SerializeField] private float frameHeight = 128f; // Match RTTTaskbar height
    [SerializeField] private float contentPadding = 40f; // Doubled margin (was 20f)
    [SerializeField] private float buttonSize = 90f;
    [SerializeField] private float buttonSpacing = 10f;
    
    [Header("Position")]
    [SerializeField] private Transform followTarget; // The Main Menu Frame
    [SerializeField] private float gapBelowFrame = 0.002f; 
    #endregion

    #region Private Fields
    private const float PixelToMeter = 1.6f / 1920f;
    
    private RTTFileManagerController _controller;
    private int _currentPage = 1;
    private int _totalPages = 1;

    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;

    private Transform _stackPagingTransform;
    private List<GameObject> _pageButtons = new List<GameObject>();
    #endregion
    
    #region Lifecycle
    public void Initialize(RTTFileManagerController controller, Transform targetFrame)
    {
        _controller = controller;
        followTarget = targetFrame;
        
        // Width Calculation: 4/3 of target RTTMiniFrame width
        if (targetFrame != null)
        {
            var targetMiniFrame = targetFrame.GetComponent<RTTMiniFrame>();
            if (targetMiniFrame != null)
            {
                frameWidth = targetMiniFrame.TotalWidth * (4f / 3f);
                frameHeight = targetMiniFrame.TotalHeight; // Sync height
                buttonSize = targetMiniFrame.ButtonSize; // Sync button size
            }
        }
        
        // Initial setup
        worldWidth = frameWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;
        
        // Initial build
        ResizeRenderTexture((int)frameWidth, (int)frameHeight);
        RebuildUI();
        
        // Ensure some initial state
        UpdateDisplay();
        
        // Ensure visible
        gameObject.SetActive(true);
    }
    
    protected override void OnDestroy()
    {
        if (_glassMaterial != null) Destroy(_glassMaterial);
        if (_borderMaterial != null) Destroy(_borderMaterial);
        base.OnDestroy();
    }
    
    protected override void LateUpdate()
    {
        base.LateUpdate();
        UpdatePositionTracking();
    }
    #endregion

    #region RTTCanvasBase Overrides
    protected override Vector2Int GetResolution()
    {
        return new Vector2Int((int)frameWidth, (int)frameHeight);
    }
    
    protected override int GetCameraDepth()
    {
         return -48; // Same depth as Expansion
    }
    
    protected override void BuildUI()
    {
        if (_canvas == null) return;
        
        var canvasRect = _canvas.GetComponent<RectTransform>();
        
        // 1. Glass Background
        CreateGlassPanel(canvasRect);
        
        // 2. Content
        CreateContent(canvasRect);
    }
    #endregion

    #region UI Construction
    private void CreateGlassPanel(RectTransform parent)
    {
        // Robust UI creation
        GameObject bgObj = new GameObject("GlassBackground", typeof(RectTransform), typeof(Image));
        bgObj.layer = LayerMask.NameToLayer("UI");
        bgObj.transform.SetParent(parent, false);

        Image img = bgObj.GetComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;
        
        // Create/Reuse Material (Simulated RTT Glass Style)
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
             _glassMaterial = new Material(glassShader);
             
             // Match RTTTaskbarExpansion settings for solid look
             _glassMaterial.SetFloat("_CornerRadius", 0.12f);
             _glassMaterial.SetFloat("_EdgePadding", 0.06f);
             _glassMaterial.SetFloat("_Aspect", frameWidth / frameHeight);
             
             // Colors and Parameters
             _glassMaterial.SetColor("_ColorA", new Color(0.0f, 0.55f, 0.65f, 0.35f));
             _glassMaterial.SetColor("_ColorB", new Color(0.30f, 0.12f, 0.50f, 0.32f));
             
             _glassMaterial.SetFloat("_CyanRatio", 0.7f);
             _glassMaterial.SetFloat("_GlassAlpha", 0.65f);
             _glassMaterial.SetFloat("_FresnelPower", 2.2f);
             _glassMaterial.SetFloat("_FresnelStrength", 0.12f);
             
             img.material = _glassMaterial;
        }
        else
        {
            img.color = new Color(0, 0, 0, 0.5f); // Fallback
        }
        
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();
        
        // Border
        CreateGlowingBorder(bgObj.transform);
    }

    private void CreateGlowingBorder(Transform parent)
    {
         GameObject borderObj = new GameObject("Border", typeof(RectTransform), typeof(Image));
         borderObj.layer = LayerMask.NameToLayer("UI");
         borderObj.transform.SetParent(parent, false);
         
         RectTransform rt = borderObj.GetComponent<RectTransform>();
         rt.anchorMin = Vector2.zero;
         rt.anchorMax = Vector2.one;
         rt.offsetMin = Vector2.zero;
         rt.offsetMax = Vector2.zero;
         
         Image img = borderObj.GetComponent<Image>();
         img.sprite = GetPixelSprite();
         img.raycastTarget = false;
         
         Shader borderShader = Shader.Find("Custom/GlowingGlassBorder");
         if (borderShader != null)
         {
             _borderMaterial = new Material(borderShader);
             _borderMaterial.SetFloat("_Aspect", frameWidth / frameHeight);
             _borderMaterial.SetFloat("_BorderWidth", 0.06f);
             _borderMaterial.SetFloat("_CornerRadius", 0.12f);
             
            // Match TaskbarExpansion border settings
            _borderMaterial.SetFloat("_Layer1Width", 0.03f);
            _borderMaterial.SetFloat("_Layer1Alpha", 1.5f);
            _borderMaterial.SetFloat("_Layer2Width", 0.06f);
            _borderMaterial.SetFloat("_Layer2Alpha", 1.0f);
            
             img.material = _borderMaterial;
         }
         borderObj.transform.SetAsLastSibling();
    }

    private void CreateContent(RectTransform parent)
    {
        GameObject container = new GameObject("Content", typeof(RectTransform));
        container.layer = LayerMask.NameToLayer("UI"); // Ensure container is in UI layer
        container.transform.SetParent(parent, false);
        
        RectTransform containerRT = container.GetComponent<RectTransform>();
        containerRT.anchorMin = Vector2.zero;
        containerRT.anchorMax = Vector2.one;
        containerRT.offsetMin = Vector2.zero;
        containerRT.offsetMax = Vector2.zero;
        containerRT.localScale = Vector3.one;
        
        // 1. Previous Button (Anchored Left)
        CreateAnchorButton(container.transform, "icon_left_arrow", OnPrevClicked, true);
        
        // 2. Next Button (Anchored Right)
        CreateAnchorButton(container.transform, "icon_right_arrow", OnNextClicked, false);
        
        // 3. Stack Paging (Centered)
        GameObject stackObj = new GameObject("StackPaging", typeof(RectTransform));
        stackObj.layer = LayerMask.NameToLayer("UI"); // Ensure stack container is in UI layer
        stackObj.transform.SetParent(container.transform, false);
        _stackPagingTransform = stackObj.transform;
        
        RectTransform stackRT = stackObj.GetComponent<RectTransform>();
        stackRT.anchorMin = new Vector2(0.5f, 0.5f);
        stackRT.anchorMax = new Vector2(0.5f, 0.5f);
        stackRT.pivot = new Vector2(0.5f, 0.5f);
        stackRT.anchoredPosition = Vector2.zero;
        stackRT.localScale = Vector3.one;
        
        HorizontalLayoutGroup stackLayout = stackObj.AddComponent<HorizontalLayoutGroup>();
        stackLayout.childAlignment = TextAnchor.MiddleCenter;
        stackLayout.spacing = buttonSpacing;
        stackLayout.childControlWidth = false;
        stackLayout.childControlHeight = false;
        stackLayout.childForceExpandWidth = false;
        stackLayout.childForceExpandHeight = false;

        // Auto-size width to fit buttons
        ContentSizeFitter csf = stackObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Force update display immediately to ensure content exists
        UpdateDisplay();
    }

    private void CreateAnchorButton(Transform parent, string iconName, UnityEngine.Events.UnityAction onClick, bool isLeft)
    {
        float navButtonSize = buttonSize * 0.8f; // Reduce by 20%

        // Wrapper for positioning
        GameObject btnWrapper = new GameObject(isLeft ? "BtnPrev" : "BtnNext", typeof(RectTransform));
        btnWrapper.layer = LayerMask.NameToLayer("UI");
        btnWrapper.transform.SetParent(parent, false);
        
        RectTransform rt = btnWrapper.GetComponent<RectTransform>();
        
        if (isLeft)
        {
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(contentPadding, 0);
        }
        else
        {
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-contentPadding, 0);
        }
        rt.sizeDelta = new Vector2(navButtonSize, navButtonSize);
        rt.localScale = Vector3.one;

        // Create actual button inside wrapper
        CreateButton(btnWrapper.transform, iconName, onClick, navButtonSize);
        
        // Reset local position of the created button (VRButtonFactory might offset it?)
        // CreateButton calls VRButtonFactory which makes a button as child of parent.
        // We need to ensure that child is centered in wrapper.
        if (btnWrapper.transform.childCount > 0)
        {
            RectTransform childRT = btnWrapper.transform.GetChild(0).GetComponent<RectTransform>();
            if (childRT != null)
            {
                childRT.anchorMin = new Vector2(0.5f, 0.5f);
                childRT.anchorMax = new Vector2(0.5f, 0.5f); 
                childRT.pivot = new Vector2(0.5f, 0.5f);
                childRT.anchoredPosition = Vector2.zero;
                childRT.localScale = Vector3.one;
            }
        }
    }
    
    private void RebuildUI()
    {
        if (_canvas != null)
        {
            foreach (Transform child in _canvas.transform)
            {
                 Destroy(child.gameObject);
            }
        }
        
        BuildUI();
    }
    
    private void CreateButton(Transform parent, string iconName, UnityEngine.Events.UnityAction onClick, float size)
    {
        // Simple Icon Button
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, 
            size, 
            Resources.Load<Sprite>(iconName), 
            Color.white, 
            onClick
        );
        
        // Debug fallback if icon missing
        if (btn != null)
        {
             // Check if icon image has sprite
            Transform visuals = btn.transform.Find("HitArea/Visuals/Content/Icon");
            if (visuals != null) {
                var img = visuals.GetComponent<Image>();
                if (img != null && img.sprite == null) {
                    Debug.LogWarning($"[RTTFilePagination] Missing icon: {iconName}");
                    img.color = Color.red; // Visual debug
                }
            }
        }
    }
    
    private GameObject CreatePageButton(Transform parent, int pageNumber, bool isActive)
    {
        GameObject btnObj = new GameObject($"Page_{pageNumber}", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.layer = LayerMask.NameToLayer("UI"); // Ensure visible to RTT Camera
        btnObj.transform.SetParent(parent, false);
        btnObj.transform.localScale = Vector3.one; // Ensure scale is 1
        
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);
        
        // Background (optional, for active state)
        Image bg = btnObj.GetComponent<Image>();
        bg.sprite = null; // Use default white sprite
        // Active: Like normal state (Faint Grey), Inactive: No background
        Color activeColor = new Color(1f, 1f, 1f, 0.1f); 
        Color inactiveColor = Color.clear; 
        bg.color = isActive ? activeColor : inactiveColor;
        
        // Button Logic
        Button btn = btnObj.GetComponent<Button>();
        btn.onClick.AddListener(() => 
        {
            _controller.GoToPage(pageNumber); 
        });
        
        // Text
        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.layer = LayerMask.NameToLayer("UI"); // Ensure text visible
        textObj.transform.SetParent(btnObj.transform, false);
        
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;
        textRT.localScale = Vector3.one;
        
        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = pageNumber.ToString();
        tmp.fontSize = 40;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;

        return btnObj;
    }

    private float _retryTimer = 0f;

    private void Update()
    {
        // Self-healing: If internally we have pages, but UI shows 1 (or 0), retry
        if (_totalPages > 1 && _stackPagingTransform != null && _stackPagingTransform.childCount < 2)
        {
            _retryTimer += Time.deltaTime;
            // Retry every 10 frames or so (0.2s)
            if (_retryTimer > 0.2f) 
            {
                Debug.LogWarning($"[RTTFilePagination] UI Mismatch detected (TotalPages: {_totalPages} vs Children: {_stackPagingTransform.childCount}). Forcing Rebuild.");
                UpdateDisplay();
                _retryTimer = 0f;
            }
        }
    }

    public void SetPage(int current, int total)
    {
        Debug.Log($"[RTTFilePagination] SetPage Called: Current={current}, TotalInput={total}");
        
        // Immediate Update - Remove Coroutine
        // Note: The Coroutine approach (previous fix) was causing issues with visibility state or frame timing conflicts.
        
        bool dataChanged = (_totalPages != total) || (_currentPage != current);
        _currentPage = current;
        _totalPages = Mathf.Max(1, total);
        
        // Always update to ensure visual sync, especially if data changed or first load
        if (dataChanged || _stackPagingTransform.childCount == 0)
        {
             UpdateDisplay();
        }
    }
    
    // Removed UpdateDisplayRoutine
    #endregion
    
    #region Logic
    private void OnPrevClicked()
    {
        _controller.ChangePage(-1);
    }
    
    private void OnNextClicked()
    {
        _controller.ChangePage(1);
    }
    
    private void UpdateDisplay()
    {
        Debug.Log($"[RTTFilePagination] UpdateDisplay: TotalPages={_totalPages}");
        if (_stackPagingTransform == null) 
        {
            Debug.LogError("[RTTFilePagination] _stackPagingTransform is NULL");
            return;
        }

        try 
        {
            // 1. Destroy Existing (Using DestroyImmediate for clean sync)
            int childCount = _stackPagingTransform.childCount;
            for (int i = childCount - 1; i >= 0; i--)
            {
                Transform child = _stackPagingTransform.GetChild(i);
                if (child != null) 
                {
                    child.SetParent(null); 
                    DestroyImmediate(child.gameObject);
                }
            }
            _pageButtons.Clear();
            
            // 2. Create New
            int startPage = 1;
            int endPage = Mathf.Max(1, _totalPages);
            
            Debug.Log($"[RTTFilePagination] Creating Buttons: {startPage} to {endPage}");
            
            for (int i = startPage; i <= endPage; i++)
            {
                int pageNum = i; 
                GameObject btn = CreatePageButton(_stackPagingTransform, pageNum, pageNum == _currentPage);
                if (btn != null)
                {
                    _pageButtons.Add(btn);
                }
            }
            
            // 3. Force Layout Logic
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_stackPagingTransform as RectTransform);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RTTFilePagination] EXCEPTION: {e}");
        }
    }
    
    private void UpdatePositionTracking()
    {
        if (followTarget == null) return;
        
        // Calculate target height
        float targetHalfHeight = 0f;
        
        // Try to get RTTMiniFrame (Taskbar usually has this)
        var targetMiniFrame = followTarget.GetComponent<RTTMiniFrame>();
        if (targetMiniFrame != null)
        {
            targetHalfHeight = targetMiniFrame.GetWorldSize().y / 2f;
        }
        else
        {
             // Try RTTCanvasBase/MenuFrame
             var targetCanvas = followTarget.GetComponent<RTTCanvasBase>();
             if (targetCanvas != null)
             {
                 targetHalfHeight = targetCanvas.GetWorldSize().y / 2f;
             }
        }
        
        float myHalfHeight = worldHeight / 2f;
        
        // Position ABOVE the target (like TaskbarExpansion)
        float totalOffset = targetHalfHeight + myHalfHeight + gapBelowFrame; 
        
        // Position: Target Pos + Up * Offset
        transform.position = followTarget.position + (followTarget.up * totalOffset);
        
        // Rotation: Same as target
        transform.rotation = followTarget.rotation;
        
        FaceCamera();
    }
    
    private void FaceCamera()
    {
        if (followTarget == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        
        Vector3 taskbarForward = followTarget.forward;
        Vector3 taskbarUp = followTarget.up;
        Vector3 toCamera = cam.transform.position - transform.position;
        if (toCamera.sqrMagnitude < 0.001f) return;
        
        Vector3 taskbarRight = followTarget.right;
        Vector3 toCameraProjected = toCamera - Vector3.Project(toCamera, taskbarRight);
        if (toCameraProjected.sqrMagnitude < 0.001f) toCameraProjected = -taskbarForward;
        
        Quaternion lookRotation = Quaternion.LookRotation(-toCameraProjected.normalized, taskbarUp);
        transform.rotation = lookRotation;
    }
    
    private Sprite GetPixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }
    #endregion
}
