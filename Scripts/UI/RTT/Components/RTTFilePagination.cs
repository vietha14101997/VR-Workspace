using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// RTTFilePagination - Pagination bar for File Manager.
/// Styled like RTTTaskbarExpansion: Floating glass panel below the main content.
/// </summary>
public class RTTFilePagination : RTTCanvasBase
{
    #region Configuration
    [Header("Layout")]
    [SerializeField] private float frameWidth = 400f; // Approx width for standard pagination
    [SerializeField] private float frameHeight = 80f;
    [SerializeField] private float contentPadding = 20f;
    
    [Header("Position")]
    [SerializeField] private Transform followTarget; // The Main Menu Frame
    [SerializeField] private float gapBelowFrame = 0.02f; 
    #endregion

    #region Private Fields
    private const float PixelToMeter = 1.6f / 1920f;
    
    private RTTFileManagerController _controller;
    private int _currentPage = 1;
    private int _totalPages = 1;

    private Material _glassMaterial;
    private Material _borderMaterial;
    private Sprite _pixelSprite;

    private TextMeshProUGUI _pageText;
    #endregion
    
    #region Lifecycle
    public void Initialize(RTTFileManagerController controller, Transform targetFrame)
    {
        _controller = controller;
        followTarget = targetFrame;
        
        // Initial setup
        worldWidth = frameWidth * PixelToMeter;
        worldHeight = frameHeight * PixelToMeter;
        
        // Initial build
        ResizeRenderTexture((int)frameWidth, (int)frameHeight);
        RebuildUI();
        
        // Ensure visible
        gameObject.SetActive(true);
    }
    
    public void SetPage(int current, int total)
    {
        _currentPage = current;
        _totalPages = total;
        UpdateDisplay();
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
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);

        Image img = bgObj.AddComponent<Image>();
        img.sprite = GetPixelSprite();
        img.raycastTarget = true;
        
        // Create/Reuse Material (Simulated RTT Glass Style)
        Shader glassShader = Shader.Find("Custom/GlassGradientBackgroundWide");
        if (glassShader != null)
        {
             _glassMaterial = new Material(glassShader);
             // Common RTT Settings
             _glassMaterial.SetFloat("_CornerRadius", 0.12f);
             _glassMaterial.SetFloat("_EdgePadding", 0.06f);
             _glassMaterial.SetFloat("_Aspect", frameWidth / frameHeight);
             _glassMaterial.SetColor("_ColorA", new Color(0.0f, 0.55f, 0.65f, 0.35f));
             _glassMaterial.SetColor("_ColorB", new Color(0.30f, 0.12f, 0.50f, 0.32f));
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
        
        // Border
        CreateGlowingBorder(bgObj.transform);
    }

    private void CreateGlowingBorder(Transform parent)
    {
         GameObject borderObj = new GameObject("Border");
         borderObj.transform.SetParent(parent, false);
         RectTransform rt = borderObj.AddComponent<RectTransform>();
         rt.anchorMin = Vector2.zero;
         rt.anchorMax = Vector2.one;
         rt.offsetMin = Vector2.zero;
         rt.offsetMax = Vector2.zero;
         
         Image img = borderObj.AddComponent<Image>();
         img.sprite = GetPixelSprite();
         
         Shader borderShader = Shader.Find("Custom/GlowingGlassBorder");
         if (borderShader != null)
         {
             _borderMaterial = new Material(borderShader);
             _borderMaterial.SetFloat("_Aspect", frameWidth / frameHeight);
             _borderMaterial.SetFloat("_BorderWidth", 0.06f);
             _borderMaterial.SetFloat("_CornerRadius", 0.12f);
             img.material = _borderMaterial;
         }
    }

    private void CreateContent(RectTransform parent)
    {
        GameObject container = new GameObject("Content");
        container.transform.SetParent(parent, false);
        RectTransform rt = container.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        
        // Horizontal Layout
        HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 30f;
        layout.padding = new RectOffset((int)contentPadding, (int)contentPadding, 10, 10);
        
        // Prev Button
        CreateButton(container.transform, "icon_arrow_left", OnPrevClicked);
        
        // Page Text
        GameObject textObj = new GameObject("PageText");
        textObj.transform.SetParent(container.transform, false);
        _pageText = textObj.AddComponent<TextMeshProUGUI>();
        _pageText.text = "1 / 1";
        _pageText.fontSize = 28;
        _pageText.alignment = TextAlignmentOptions.Center;
        _pageText.color = Color.white;
        _pageText.enableWordWrapping = false;
        
        // Next Button
        CreateButton(container.transform, "icon_arrow_right", OnNextClicked);
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
    
    private void CreateButton(Transform parent, string iconName, UnityEngine.Events.UnityAction onClick)
    {
        // Simple Icon Button
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, 
            50f, 
            Resources.Load<Sprite>(iconName), 
            Color.white, 
            onClick
        );
    }
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
        if (_pageText != null) _pageText.text = $"{_currentPage} / {_totalPages}";
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
        float totalOffset = targetHalfHeight + myHalfHeight + gapBelowFrame; // gapBelowFrame is just a gap value, we can rename variable or keep as is used for gap
        
        // Position: Target Pos + Up * Offset
        transform.position = followTarget.position + (followTarget.up * totalOffset);
        
        // Rotation: Same as target
        transform.rotation = followTarget.rotation;
        
        // Optional: Face camera tilt like Expansion?
        // User asked to be "like RTTTaskbarExpansion", which implies facing logic too.
        // Expansion faces camera on X axis but keeps Y from taskbar.
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
