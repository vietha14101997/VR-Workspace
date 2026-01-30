using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
    [SerializeField] private float frameHeight = 115f; // 90% of RTTTaskbar height
    [SerializeField] private float contentPadding = 40f; // Doubled margin (was 20f)
    [SerializeField] private float buttonSize = 90f;
    [SerializeField] private float buttonSpacing = 10f;
    
    // Note: Positioning is handled by RTTToolbar parent
    #endregion

    #region Private Fields
    private const float PixelToMeter = 1.6f / 1920f;

    // Shared highlight color for both hover and selected (dark transparent)
    private static readonly Color HighlightColorA = new Color(0f, 0f, 0f, 0.27f);
    private static readonly Color HighlightColorB = new Color(0f, 0f, 0f, 0.22f);
    private static readonly Color TransparentColor = new Color(0f, 0f, 0f, 0f);
    private const float HighlightGlassAlpha = 0.45f;

    private IPaginationController _controller;
    private int _currentPage = 1;
    private int _totalPages = 1;

    private Material _glassMaterial;
    private Sprite _pixelSprite;

    private Transform _stackPagingTransform;
    private Transform _leftGroupTransform;
    private Transform _rightGroupTransform;
    private List<GameObject> _pageButtons = new List<GameObject>();
    
    private Button _btnPrev;
    private Button _btnNext;
    #endregion
    
    #region Lifecycle
    private bool _initialized = false;
    private Coroutine _fadeCoroutine = null;
    private bool _pendingDisplayUpdate = false; // Track if UpdateDisplay() was deferred during fade
    private bool _isShown = false; // Track if pagination has been shown (to prevent OnEnable resetting alpha)
    private float _lastShowTime = 0f; // Track when Show() was called to prevent rapid hide/show flicker
    private const float FADE_DURATION = 0.15f; // Match RTTManager's transitionInDuration
    private const float HIDE_GRACE_PERIOD = 0.5f; // Don't hide within this time after Show()

    protected override void OnEnable()
    {
        base.OnEnable();

        float currentAlpha = GetQuadAlpha();
        Debug.Log($"[RTTFilePagination] OnEnable: _initialized={_initialized}, _fadeCoroutine={(_fadeCoroutine != null ? "running" : "null")}, _isShown={_isShown}, alpha={currentAlpha:F2}");

        // CRITICAL: Only reset alpha if NOT already shown and no fade is in progress
        // This prevents OnEnable from resetting alpha after fade has completed
        // _isShown is true when Show() has been called and fade has completed
        if (_initialized && _fadeCoroutine == null && !_isShown)
        {
            Debug.Log($"[RTTFilePagination] OnEnable: RESETTING alpha to 0 (was {currentAlpha:F2})");
            SetQuadAlpha(0f);
        }
    }

    public void Initialize(RTTFileManagerController controller)
    {
        Initialize((IPaginationController)controller);
    }

    public void Initialize(IPaginationController controller)
    {
        // CRITICAL: Disable object BEFORE creating any visuals to prevent flicker
        gameObject.SetActive(false);

        _controller = controller;
        // Note: Positioning is handled by RTTToolbar parent

        // Width Calculation: Get size from RTTTaskbar's RTTMiniFrame (not from followTarget)
        // followTarget is now the main menu, but we still want pagination sized relative to taskbar
        RTTMiniFrame taskbarMiniFrame = null;
        if (RTTTaskbar.Instance != null)
        {
            taskbarMiniFrame = RTTTaskbar.Instance.GetComponent<RTTMiniFrame>();
        }

        if (taskbarMiniFrame != null)
        {
            frameWidth = taskbarMiniFrame.TotalWidth * (4f / 3f);
            frameHeight = taskbarMiniFrame.TotalHeight * 0.9f; // Reduced height by 10%
            buttonSize = taskbarMiniFrame.ButtonSize; // Sync button size
        }

        // Initial setup
        worldWidth = frameWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;

        // Build UI while object is inactive (won't render)
        ResizeRenderTexture((int)frameWidth, (int)frameHeight);
        RebuildUI();
        UpdateDisplay();

        // Set initial alpha to 0 for fade-in animation
        SetQuadAlpha(0f);

        _initialized = true;
        // Object stays inactive until Show() is called
    }

    public new void Show()
    {
        float currentAlpha = GetQuadAlpha();
        Debug.Log($"[RTTFilePagination] Show() called: _isShown={_isShown}, activeSelf={gameObject.activeSelf}, alpha={currentAlpha:F2}, _fadeCoroutine={(_fadeCoroutine != null ? "running" : "null")}");

        // Skip if already shown and visible
        if (_isShown && gameObject.activeSelf && GetQuadAlpha() >= 0.95f)
        {
            Debug.Log("[RTTFilePagination] Show() SKIPPED: already shown and visible");
            return;
        }

        // Skip if fade is already in progress
        if (_fadeCoroutine != null)
        {
            Debug.Log("[RTTFilePagination] Show() SKIPPED: fade in progress");
            return;
        }

        Debug.Log("[RTTFilePagination] Show() STARTING fade-in animation");

        // Track when Show() was called to prevent rapid hide/show flicker
        _lastShowTime = Time.time;

        // CRITICAL: Set alpha to 0 BEFORE activating to prevent flash
        SetQuadAlpha(0f);

        // Activate the object (OnEnable will NOT reset alpha because _isShown check comes after)
        gameObject.SetActive(true);

        // Double-check alpha is 0 after activation
        SetQuadAlpha(0f);

        // Start fade in animation
        _fadeCoroutine = StartCoroutine(FadeIn());
    }

    private System.Collections.IEnumerator FadeIn()
    {
        Debug.Log("[RTTFilePagination] FadeIn STARTED");
        float elapsed = 0f;
        int frameCount = 0;
        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / FADE_DURATION);
            // Ease out for smooth appearance
            float alpha = 1f - Mathf.Pow(1f - t, 2f);
            SetQuadAlpha(alpha);
            frameCount++;
            yield return null;
        }

        SetQuadAlpha(1f);
        _fadeCoroutine = null;
        _isShown = true; // Mark as shown to prevent OnEnable from resetting alpha

        Debug.Log($"[RTTFilePagination] FadeIn COMPLETED: frames={frameCount}, _isShown={_isShown}, alpha={GetQuadAlpha():F2}");

        // Process pending display update if SetPage() was called during fade
        if (_pendingDisplayUpdate)
        {
            Debug.Log("[RTTFilePagination] FadeIn: Processing pending UpdateDisplay()");
            _pendingDisplayUpdate = false;
            UpdateDisplay();
        }
    }

    private void SetQuadAlpha(float alpha)
    {
        var quad = GetDisplayQuad();
        if (quad != null && quad.material != null)
        {
            Color c = quad.material.color;
            c.a = alpha;
            quad.material.color = c;
        }
    }

    private float GetQuadAlpha()
    {
        var quad = GetDisplayQuad();
        if (quad != null && quad.material != null)
        {
            return quad.material.color.a;
        }
        return 1f;
    }

    public new void Hide()
    {
        float timeSinceShow = Time.time - _lastShowTime;
        Debug.Log($"[RTTFilePagination] Hide() called: _isShown={_isShown}, _fadeCoroutine={(_fadeCoroutine != null ? "running" : "null")}, alpha={GetQuadAlpha():F2}, timeSinceShow={timeSinceShow:F2}s");

        // CRITICAL: Prevent rapid hide/show flicker by ignoring Hide() calls too soon after Show()
        // This fixes the issue where SelectSlot() triggers OnDisable/OnEnable after fade completes
        if (_isShown && timeSinceShow < HIDE_GRACE_PERIOD)
        {
            Debug.Log($"[RTTFilePagination] Hide() SKIPPED: within grace period ({timeSinceShow:F2}s < {HIDE_GRACE_PERIOD}s)");
            return;
        }

        // Cancel any pending fade
        if (_fadeCoroutine != null)
        {
            Debug.Log("[RTTFilePagination] Hide(): Stopping fade coroutine");
            StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        // Clear pending display update when hiding
        _pendingDisplayUpdate = false;

        // Reset shown state so next Show() will work correctly
        _isShown = false;

        // Immediately hide - no fade out to prevent race conditions during app switching
        // The frame transition already handles visual continuity
        SetQuadAlpha(0f);
        gameObject.SetActive(false);
        Debug.Log("[RTTFilePagination] Hide() completed: _isShown=false, alpha=0, active=false");
    }
    
    protected override void OnDestroy()
    {
        if (_glassMaterial != null) Destroy(_glassMaterial);
        base.OnDestroy();
    }
    
    protected override void LateUpdate()
    {
        base.LateUpdate();
        // Note: Positioning is handled by RTTToolbar parent
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
             // CornerRadius = 0.5 để bo bán cầu 2 cạnh trái phải
             _glassMaterial.SetFloat("_CornerRadius", 0.5f);
             _glassMaterial.SetFloat("_EdgePadding", 0.0f);
             _glassMaterial.SetFloat("_Aspect", frameWidth / frameHeight);
             
             // Colors and Parameters - Brighter to match MenuFrame/MiniFrame
             _glassMaterial.SetColor("_ColorA", new Color(0.15f, 0.65f, 0.75f, 0.28f));
             _glassMaterial.SetColor("_ColorB", new Color(0.40f, 0.22f, 0.60f, 0.25f));

             _glassMaterial.SetFloat("_CyanRatio", 0.7f);
             _glassMaterial.SetFloat("_GlassAlpha", 0.55f);
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
        _btnPrev = CreateAnchorButton(container.transform, "icon_left_arrow", OnPrevClicked, true);
        
        // 2. Next Button (Anchored Right)
        _btnNext = CreateAnchorButton(container.transform, "icon_right_arrow", OnNextClicked, false);
        
        // 3. Stack Paging (Centered - Now holds EVERYTHING to ensure even spacing)
        GameObject stackObj = new GameObject("StackPaging", typeof(RectTransform));
        stackObj.layer = LayerMask.NameToLayer("UI");
        stackObj.transform.SetParent(container.transform, false);
        _stackPagingTransform = stackObj.transform;
        
        RectTransform stackRT = stackObj.GetComponent<RectTransform>();
        stackRT.anchorMin = new Vector2(0.5f, 0.5f);
        stackRT.anchorMax = new Vector2(0.5f, 0.5f);
        stackRT.pivot = new Vector2(0.5f, 0.5f);
        stackRT.anchoredPosition = Vector2.zero;
        
        HorizontalLayoutGroup stackLayout = stackObj.AddComponent<HorizontalLayoutGroup>();
        stackLayout.childAlignment = TextAnchor.MiddleCenter;
        stackLayout.spacing = buttonSpacing; // Uniform spacing for all items
        stackLayout.childControlWidth = false;
        stackLayout.childControlHeight = false;
        stackLayout.childForceExpandWidth = false;
        stackLayout.childForceExpandHeight = false;
        
        ContentSizeFitter csf = stackObj.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Force update display immediately
        UpdateDisplay();
    }

    // Removed CreateSubGroup as it is no longer needed

    private Button CreateAnchorButton(Transform parent, string iconName, UnityEngine.Events.UnityAction onClick, bool isLeft)
    {
        float iconSize = buttonSize * 0.6f; // Icon size inside button

        // Wrapper for positioning (same size as button)
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
        rt.sizeDelta = new Vector2(buttonSize, buttonSize); // Same size as page buttons
        rt.localScale = Vector3.one;

        // Create button inside wrapper
        // Shift icon 5% towards its side (Left or Right)
        float offsetX = isLeft ? -(buttonSize * 0.05f) : (buttonSize * 0.05f);
        return CreateButton(btnWrapper.transform, iconName, onClick, iconSize, new Vector2(offsetX, 0));
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
    
    private Button CreateButton(Transform parent, string iconName, UnityEngine.Events.UnityAction onClick, float size, Vector2 iconOffset)
    {
        // Create simple button similar to page buttons (not using VRButtonFactory)
        GameObject btnObj = new GameObject(iconName, typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.layer = LayerMask.NameToLayer("UI");
        btnObj.transform.SetParent(parent, false);
        btnObj.transform.localScale = Vector3.one;

        // Fill the parent wrapper completely to fix centering issue
        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Circular Background
        Image bg = btnObj.GetComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = true;

        Material circleMat = CreateCircleButtonMaterial(false);
        if (circleMat != null) bg.material = circleMat;

        // Button click
        Button btn = btnObj.GetComponent<Button>();
        btn.transition = Selectable.Transition.None; // Disable default tint to avoid conflict with shader
        btn.onClick.AddListener(onClick);

        // Add hover effect
        AddHoverEffect(btnObj, circleMat);

        // Icon (centered using center anchors for proper alignment)
        GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconObj.layer = LayerMask.NameToLayer("UI");
        iconObj.transform.SetParent(btnObj.transform, false);

        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.pivot = new Vector2(0.5f, 0.5f);
        iconRT.anchoredPosition = iconOffset;
        iconRT.sizeDelta = new Vector2(size, size);
        iconRT.localScale = Vector3.one;

        Image iconImg = iconObj.GetComponent<Image>();
        iconImg.sprite = Resources.Load<Sprite>(iconName);
        iconImg.color = Color.white;
        iconImg.raycastTarget = false;
        iconImg.preserveAspect = true; // Ensure icon content is centered within bounds

        if (iconImg.sprite == null)
        {
            Debug.LogWarning($"[RTTFilePagination] Missing icon: {iconName}");
            iconImg.color = Color.red;
        }

        return btn;
    }

    private void SetButtonState(Button btn, bool active)
    {
        if (btn == null) return;
        btn.interactable = active;
        
        // Dim Icon
        var icon = btn.transform.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
        {
             icon.color = active ? Color.white : new Color(1, 1, 1, 0.2f);
        }
        
        // Disable Hover Component to prevent visual updates
        var hover = btn.GetComponent<PaginationButtonHover>();
        if (hover != null) hover.enabled = active;
        
        // Reset Background to transparent if disabled
        if (!active)
        {
             Material mat = btn.GetComponent<Image>().material;
             if (mat != null)
             {
                 mat.SetColor("_ColorA", TransparentColor);
                 mat.SetColor("_ColorB", TransparentColor);
                 mat.SetFloat("_GlassAlpha", 0f);
             }
        }
    }

    private Material CreateCircleButtonMaterial(bool isActive)
    {
        Shader circleShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (circleShader == null) return null;

        Material mat = new Material(circleShader);
        mat.SetFloat("_CornerRadius", 0.5f);
        mat.SetFloat("_EdgePadding", 0.0f);
        mat.SetFloat("_Aspect", 1f);
        mat.SetFloat("_CyanRatio", 0.5f);
        mat.SetFloat("_FresnelPower", 2.2f);
        mat.SetFloat("_FresnelStrength", 0f);

        if (isActive)
        {
            // Active/Selected state: same highlight color
            mat.SetColor("_ColorA", HighlightColorA);
            mat.SetColor("_ColorB", HighlightColorB);
            mat.SetFloat("_GlassAlpha", HighlightGlassAlpha);
        }
        else
        {
            // Fully transparent initially
            mat.SetColor("_ColorA", TransparentColor);
            mat.SetColor("_ColorB", TransparentColor);
            mat.SetFloat("_GlassAlpha", 0f);
        }

        return mat;
    }

    private void AddHoverEffect(GameObject target, Material circleMat)
    {
        if (circleMat == null) return;

        // Use IPointerEnterHandler/IPointerExitHandler for VR compatibility
        var hoverEffect = target.AddComponent<PaginationButtonHover>();
        hoverEffect.Initialize(circleMat, false); // Arrow buttons are never "active"
    }

    private void AddPageButtonHoverEffect(GameObject target, Material circleMat, bool isActive)
    {
        if (circleMat == null) return;

        // Use IPointerEnterHandler/IPointerExitHandler for VR compatibility
        var hoverEffect = target.AddComponent<PaginationButtonHover>();
        hoverEffect.Initialize(circleMat, isActive);
    }
    
    /// <summary>
    /// Position type for special scroll behavior on first/last buttons.
    /// </summary>
    private enum PageButtonPosition { Normal, First, Last }

    private GameObject CreatePageButton(Transform parent, int pageNumber, bool isActive, PageButtonPosition position = PageButtonPosition.Normal)
    {
        GameObject btnObj = new GameObject($"Page_{pageNumber}", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.layer = LayerMask.NameToLayer("UI");
        btnObj.transform.SetParent(parent, false);
        btnObj.transform.localScale = Vector3.one;

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        // Circular Background with shader
        Image bg = btnObj.GetComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.raycastTarget = true; // Ensure hover/click works

        Material circleMat = CreateCircleButtonMaterial(isActive);
        if (circleMat != null)
        {
            bg.material = circleMat;
        }
        else
        {
            // Fallback if no shader
            bg.color = isActive ? new Color(1f, 1f, 1f, 0.2f) : Color.clear;
        }

        // Button Logic
        Button btn = btnObj.GetComponent<Button>();
        btn.transition = Selectable.Transition.None; // Disable default transition to avoid interference
        btn.interactable = !isActive;

        // Special click behavior for first/last visible buttons
        if (position == PageButtonPosition.First)
        {
            btn.onClick.AddListener(() => _controller.ScrollToStart());
        }
        else if (position == PageButtonPosition.Last)
        {
            btn.onClick.AddListener(() => _controller.ScrollToEnd());
        }
        else
        {
            btn.onClick.AddListener(() => _controller.GoToPage(pageNumber));
        }

        // Hover Effect - apply to ALL buttons (even active)
        AddPageButtonHoverEffect(btnObj, circleMat, isActive);

        // Text
        GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.layer = LayerMask.NameToLayer("UI");
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
        tmp.raycastTarget = false; // CRITICAL: Disable raycast on text so it doesn't block hover events on the button

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
        bool dataChanged = (_totalPages != total) || (_currentPage != current);
        Debug.Log($"[RTTFilePagination] SetPage({current}, {total}): dataChanged={dataChanged}, _fadeCoroutine={(_fadeCoroutine != null ? "running" : "null")}, _isShown={_isShown}");

        _currentPage = current;
        _totalPages = Mathf.Max(1, total);

        // Defer UpdateDisplay() if fade is in progress to prevent flicker
        // The UI will be rebuilt when fade completes
        if (_fadeCoroutine != null)
        {
            if (dataChanged || _stackPagingTransform.childCount == 0)
            {
                Debug.Log("[RTTFilePagination] SetPage: DEFERRING UpdateDisplay (fade in progress)");
                _pendingDisplayUpdate = true;
            }
            // Still update nav buttons state even during fade
            UpdateNavigationButtonsState();
            return;
        }

        // Not fading - update display immediately if needed
        if (dataChanged || _stackPagingTransform.childCount == 0)
        {
            Debug.Log("[RTTFilePagination] SetPage: Calling UpdateDisplay immediately");
            UpdateDisplay();
        }

        // Ensure nav buttons state is updated
        UpdateNavigationButtonsState();
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
    
    private void ClearTransform(Transform t)
    {
        if (t == null) return;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Transform child = t.GetChild(i);
            child.SetParent(null);
            DestroyImmediate(child.gameObject);
        }
    }

    private void UpdateDisplay()
    {
        // Debug.Log($"[RTTFilePagination] UpdateDisplay: TotalPages={_totalPages}");
        UpdateNavigationButtonsState(); // Sync nav buttons first

        if (_stackPagingTransform == null) return;

        try 
        {
            _pageButtons.Clear();
            
            // Clear all groups
            ClearTransform(_stackPagingTransform);
            // Left/Right groups are gone, only stack remains

            int maxCentralButtons = 7;
            int startPage = 1;
            int endPage = _totalPages;

            bool showStart = false;
            bool showEnd = false;

            if (_totalPages > maxCentralButtons)
            {
                int halfWindow = maxCentralButtons / 2;
                startPage = Mathf.Max(1, _currentPage - halfWindow);
                endPage = startPage + maxCentralButtons - 1;
                
                // Adjustment if near end
                if (endPage > _totalPages)
                {
                    endPage = _totalPages;
                    startPage = Mathf.Max(1, endPage - maxCentralButtons + 1);
                }

                if (startPage > 1) showStart = true;
                if (endPage < _totalPages) showEnd = true;
            }
            
            // 1. Render Left Group Elements (into Stack)
            if (showStart)
            {
                // First visible button -> scroll to start
                CreatePageButton(_stackPagingTransform, 1, 1 == _currentPage, PageButtonPosition.First);
                if (startPage > 2) CreateEllipsisButton(_stackPagingTransform);
            }

            // 2. Render Central Stack Elements (into Stack)
            for (int i = startPage; i <= endPage; i++)
            {
                // Determine position for first/last button in central range
                PageButtonPosition pos = PageButtonPosition.Normal;
                if (!showStart && i == startPage) pos = PageButtonPosition.First;
                if (!showEnd && i == endPage) pos = PageButtonPosition.Last;

                GameObject btn = CreatePageButton(_stackPagingTransform, i, i == _currentPage, pos);
                if (btn != null) _pageButtons.Add(btn);
            }

            // 3. Render Right Group Elements (into Stack)
            if (showEnd)
            {
                if (endPage < _totalPages - 1) CreateEllipsisButton(_stackPagingTransform);
                // Last visible button -> scroll to end
                CreatePageButton(_stackPagingTransform, _totalPages, _totalPages == _currentPage, PageButtonPosition.Last);
            }
            
            // 4. Force Layout Logic
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_stackPagingTransform as RectTransform);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RTTFilePagination] EXCEPTION: {e}");
        }
    }
    
    private void CreateEllipsisButton(Transform parent)
    {
         GameObject btnObj = new GameObject("Ellipsis", typeof(RectTransform), typeof(Image));
         btnObj.layer = LayerMask.NameToLayer("UI");
         btnObj.transform.SetParent(parent, false);
         btnObj.transform.localScale = Vector3.one;
         
         RectTransform rt = btnObj.GetComponent<RectTransform>();
         rt.sizeDelta = new Vector2(buttonSize * 0.5f, buttonSize); // Narrower
         
         Image bg = btnObj.GetComponent<Image>();
         bg.color = Color.clear; // Transparent
         
         GameObject textObj = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
         textObj.transform.SetParent(btnObj.transform, false);
         
         RectTransform textRT = textObj.GetComponent<RectTransform>();
         textRT.anchorMin = Vector2.zero;
         textRT.anchorMax = Vector2.one;
         textRT.offsetMin = Vector2.zero;
         textRT.offsetMax = Vector2.zero;
         
         TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
         tmp.text = "...";
         tmp.fontSize = 40;
         tmp.alignment = TextAlignmentOptions.Center;
         tmp.color = new Color(1f, 1f, 1f, 0.5f);
         tmp.fontStyle = FontStyles.Bold;
    }
    
    private void UpdateNavigationButtonsState()
    {
        bool prevActive = _currentPage > 1;
        bool nextActive = _currentPage < _totalPages;
        
        SetButtonState(_btnPrev, prevActive);
        SetButtonState(_btnNext, nextActive);
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

/// <summary>
/// Helper component for pagination button hover effects.
/// Uses IPointerEnterHandler/IPointerExitHandler for VR compatibility.
/// </summary>
public class PaginationButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Material _material;
    private Color _originalColorA;
    private Color _originalColorB;
    private float _originalAlpha;
    private Color _hoverColorA;
    private Color _hoverColorB;
    private float _hoverAlpha;

    public void Initialize(Material mat, bool isActive)
    {
        _material = mat;

        // Shared highlight color for both hover and selected
        Color highlightA = new Color(0f, 0f, 0f, 0.27f);
        Color highlightB = new Color(0f, 0f, 0f, 0.22f);
        float highlightAlpha = 0.45f;

        // Original colors based on active state
        _originalColorA = isActive ? highlightA : new Color(0f, 0f, 0f, 0f);
        _originalColorB = isActive ? highlightB : new Color(0f, 0f, 0f, 0f);
        _originalAlpha = isActive ? highlightAlpha : 0f;

        // Hover uses the same highlight color
        _hoverColorA = highlightA;
        _hoverColorB = highlightB;
        _hoverAlpha = highlightAlpha;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Respect interactable state
        if (GetComponent<Button>() != null && !GetComponent<Button>().interactable) return;

        if (_material == null) return;
        _material.SetColor("_ColorA", _hoverColorA);
        _material.SetColor("_ColorB", _hoverColorB);
        _material.SetFloat("_GlassAlpha", _hoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_material == null) return;
        _material.SetColor("_ColorA", _originalColorA);
        _material.SetColor("_ColorB", _originalColorB);
        _material.SetFloat("_GlassAlpha", _originalAlpha);
    }
}
