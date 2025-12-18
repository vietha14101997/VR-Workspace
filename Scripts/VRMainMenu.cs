using UnityEngine;
using UnityEngine.UI;
using TMPro; 
using System.Collections.Generic; 
using System; 
using Random = UnityEngine.Random; 
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VRMainMenu : MonoBehaviour
{
    [Header("Configuration")]
    public float panelWidth = 1.6f;
    public float panelHeight = 0.9f;

 
    
    // Cyberpunk Neon Palette
    public Color[] buttonColors = new Color[] {
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.7568627450980392f, 0.3568627450980392f, 1.0f, 1.0f), 
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
        new Color(0.7568627450980392f, 0.3568627450980392f, 1.0f, 1.0f), 
        new Color(0.0f, 0.9019607843137255f, 1.0f, 1.0f),
    };
    
    public int fontSize = 36;

    [Header("Typography")]
    public TMP_FontAsset customFont;

    [Header("Icons")]
    public Sprite iconRemote;
    public Sprite iconBrowser;
    public Sprite iconMedia;
    public Sprite iconFiles;
    public Sprite iconSettings;
    public Sprite iconQuit;
    




    private Canvas _canvas;
    private GameObject _gridContainer;
    private Sprite _pixelSprite;
    
    // Status References and other cache removed as they are now in VRMenuFrame or unused
    private VRMenuFrame _menuFrame;






    [ContextMenu("Rebuild UI")]
    public void ManualRebuild()
    {
        LoadIcons();
#if UNITY_EDITOR
        FixIconImportSettings();
#endif
        if (_canvas) DestroyImmediate(_canvas.gameObject);
        if (_gridContainer) DestroyImmediate(_gridContainer);
        var existingCanvas = transform.Find("MenuCanvas");
        if (existingCanvas) DestroyImmediate(existingCanvas.gameObject);
        
        BuildInterface();
    }

    void Start()
    {
#if UNITY_EDITOR
        FixIconImportSettings(); // Auto-fix on Start in Editor
#endif
        LoadIcons();
        BuildInterface();
    }


    // Update removed (handled by VRMenuFrame)





#if UNITY_EDITOR
    void FixIconImportSettings()
    {
        string[] iconNames = { 
            "icon_remote", "icon_browser", "icon_media", 
            "icon_files", "icon_settings", "icon_quit"
        };

        foreach (var name in iconNames)
        {
            try {
                string path = $"Assets/VR-Workspace/Resources/MainMenu/{name}.png";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.SaveAndReimport();
                }
            } catch {}
        }
    }
#endif




    void LoadIcons()
    {
        if (iconRemote == null) iconRemote = Resources.Load<Sprite>("MainMenu/icon_remote");
        if (iconBrowser == null) iconBrowser = Resources.Load<Sprite>("MainMenu/icon_browser");
        if (iconMedia == null) iconMedia = Resources.Load<Sprite>("MainMenu/icon_media");
        if (iconFiles == null) iconFiles = Resources.Load<Sprite>("MainMenu/icon_files");
        if (iconSettings == null) iconSettings = Resources.Load<Sprite>("MainMenu/icon_settings");
        if (iconQuit == null) iconQuit = Resources.Load<Sprite>("MainMenu/icon_quit");

    }




    Sprite GetPixelSprite()
    {
        if (_pixelSprite) return _pixelSprite;
        Texture2D tex = new Texture2D(2, 2);
        tex.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f);
        return _pixelSprite;
    }

    // Battery Sprite Helper Removed

    void BuildInterface()
    {
        Debug.Log("[VRMainMenu] Building Cyberpunk Interface V24 via VRMenuFrame...");

        GameObject canvasGO = new GameObject("MenuCanvas");
        canvasGO.transform.SetParent(transform, false); 

        float logicalWidth = 1920f;
        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;

        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        
        RectTransform canvasRT = canvasGO.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(logicalWidth, logicalHeight);
        float scaleFactor = panelWidth / logicalWidth;
        canvasRT.localScale = new Vector3(scaleFactor, scaleFactor, 1f);
        canvasRT.localPosition = new Vector3(0, 0, 0); 
        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        
        // --- ADD FRAME ---
        _menuFrame = canvasGO.AddComponent<VRMenuFrame>();
        // Pass any manual overrides if needed, primarily fonts or specific assets if not Auto-loaded
        _menuFrame.customFont = customFont;
        
        _menuFrame.Build(logicalWidth, logicalHeight);
        
        // --- ADD CONTENT ---
        ShowMainMenu();

        // --- FIX LAYER: Ensure UI has VirtualObjects layer ---
        int layerVO = LayerMask.NameToLayer("VirtualObjects");
        if (layerVO != -1) SetLayerRecursively(canvasGO, layerVO);
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child == null) continue;
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }
    
    // CreateGlassPanel, CreateFloatingDataEffects, CreateStatusBar removed (moved to VRMenuFrame)

    public void ShowMainMenu()
    {
        // Clear Content
        foreach (Transform child in _menuFrame.ContentContainer)
        {
            Destroy(child.gameObject);
        }
        _gridContainer = null;

        float logicalWidth = 1920f; 
        float logicalHeight = (logicalWidth / panelWidth) * panelHeight;
        float contentHeight = logicalHeight - _menuFrame.topMargin;
        
        
        BuildMenuLayout(_menuFrame.ContentContainer, logicalWidth, contentHeight, 50f);
        
        Canvas.ForceUpdateCanvases();
        if (_gridContainer) {
            var fitter = _gridContainer.GetComponent<GridLayoutGroup>();
            if(fitter) {
                fitter.CalculateLayoutInputHorizontal();
                fitter.CalculateLayoutInputVertical();
                fitter.SetLayoutHorizontal();
                fitter.SetLayoutVertical();
            }
        }
    }
    
    public void SwitchToRemoteMenu()
    {
        // Clear Content
        foreach (Transform child in _menuFrame.ContentContainer)
        {
            Destroy(child.gameObject);
        }
        _gridContainer = null; 

        // Create Remote Menu
        GameObject remoteObj = new GameObject("VRRemoteMenu_Logic");
        remoteObj.transform.SetParent(_menuFrame.ContentContainer, false);
        VRRemoteMenu remoteMenu = remoteObj.AddComponent<VRRemoteMenu>();
        remoteMenu.customFont = customFont; 
        remoteMenu.themeColor = buttonColors[0]; 

        remoteMenu.BuildUI(_menuFrame.ContentContainer, this);
    }

    public void ReturnToMainMenu()
    {
        ShowMainMenu();
    }

    void BuildMenuLayout(Transform parent, float w, float h, float topMargin)
    {
        _gridContainer = new GameObject("MenuLayout");
        _gridContainer.transform.SetParent(parent, false);
        
        RectTransform rt = _gridContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(100f, 50f); // Side/Bottom padding
        rt.offsetMax = new Vector2(-100f, -topMargin); // Side/Top padding
        
        // Vertical Layout
        VerticalLayoutGroup vLayout = _gridContainer.AddComponent<VerticalLayoutGroup>();
        vLayout.spacing = 60f; // Increased spacing
        vLayout.childAlignment = TextAnchor.MiddleCenter; // Center rows vertically
        vLayout.childControlHeight = true;
        vLayout.childControlWidth = true;
        
        // --- ROW 1: APPS (Remote, Browser, Media) ---
        GameObject row1 = CreateRow(_gridContainer.transform, "Row_Apps", 1.0f);
        CreateButtonInRow(row1.transform, "Remote Desktop", iconRemote, buttonColors[0], () => OpenRemoteDesktop());
        CreateButtonInRow(row1.transform, "Browser", iconBrowser, buttonColors[1], () => Debug.Log("Browser"));
        CreateButtonInRow(row1.transform, "Media", iconMedia, buttonColors[2], () => Debug.Log("Media"));

        // --- ROW 2: TOOLS (Files, Resolution, FPS) ---
        // Equal height weighting (1.0f) for uniform grid look
        GameObject row2 = CreateRow(_gridContainer.transform, "Row_Tools", 1.0f); 
        
        // Files (Standard)
        CreateButtonInRow(row2.transform, "Files", iconFiles, buttonColors[3], () => Debug.Log("Files"), true);
        
        // Resolution (Custom Logic placeholder) - Using Settings Icon for now
        CreateButtonInRow(row2.transform, "Resolution\n1280 - 720", iconSettings, buttonColors[4], () => Debug.Log("Resolution"), true);
        
        // FPS (Custom Logic placeholder) - Using Settings Icon for now
        CreateButtonInRow(row2.transform, "FPS\n60", iconSettings, buttonColors[4], () => Debug.Log("FPS"), true);

        // --- ROW 3 REMOVED (Connect Button) ---
    }

    GameObject CreateRow(Transform parent, string name, float flexibleHeight)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        RectTransform rt = row.AddComponent<RectTransform>();
        
        HorizontalLayoutGroup hg = row.AddComponent<HorizontalLayoutGroup>();
        hg.spacing = 60f; // Increased spacing
        hg.childAlignment = TextAnchor.MiddleCenter;
        hg.childControlWidth = true;
        hg.childControlHeight = true;
        
        LayoutElement le = row.AddComponent<LayoutElement>();
        le.flexibleHeight = flexibleHeight; // Weight
        le.flexibleWidth = 1f;
        
        return row;
    }
    
    // Wrapper to adapt the old CreateGridButton logic to the new Layout system
    void CreateButtonInRow(Transform parent, string label, Sprite icon, Color btnColor, UnityEngine.Events.UnityAction action, bool showDropdown = false, bool isWideAction = false)
    {
        // 1. Layout Element (Cell)
        GameObject wrapper = new GameObject("Btn_" + label);
        wrapper.transform.SetParent(parent, false);
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
        
        // Layout Config
        LayoutElement le = wrapper.AddComponent<LayoutElement>();
        le.flexibleWidth = 0f; // Disable flexible
        le.flexibleHeight = 0f; // Disable flexible
        le.preferredWidth = 400f; // Fixed size
        le.preferredHeight = 320f; // Fixed size
        
        // If it's the "Connect" button, we might want different visual properties
        // For now, we reuse the inner logic. 
        
        // 2. We skip the rigid size calculation from before and rely on RectTransform stretching
        // generated by the Horizontal layout.
        
        CreateFlexibleButton(wrapper, label, icon, btnColor, action, showDropdown, isWideAction);
    }

    void CreateFlexibleButton(GameObject parent, string label, Sprite icon, Color btnColor, UnityEngine.Events.UnityAction onClick, bool showDropdown, bool isWideAction)
    {
        // Parent is the Wrapper from LayoutGroup
        
        // 1. Hit Area 
        GameObject btnHitObj = new GameObject("HitArea");
        btnHitObj.transform.SetParent(parent.transform, false);
        RectTransform hitRT = btnHitObj.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero; hitRT.anchorMax = Vector2.one;
        hitRT.offsetMin = Vector2.zero; hitRT.offsetMax = Vector2.zero;
        
        Image hitImg = btnHitObj.AddComponent<Image>();
        hitImg.color = Color.clear;
        
        // Collider - We need to wait for layout or use a fixed approximation?
        // Since we are flexible, BoxCollider size is tricky.
        // SOLUTION: We add a component that updates Collider size on RectTransform change?
        // For now, we set a default substantial size.
        BoxCollider col = btnHitObj.AddComponent<BoxCollider>();
        col.size = new Vector3(200, 100, 0.1f); // Needs dynamic update ideally
        
        // Keep checking size in Update or similar if critical. 
        // Or assume the button is roughly X by Y.
        
        // 2. Visual Root
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btnHitObj.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        
        // Expansion for Glow/Border
        float expansion = isWideAction ? 0.05f : 0.08f; 
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero; visRT.offsetMax = Vector2.zero;
        
        // Background
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.type = Image.Type.Simple;
        bg.raycastTarget = false;
        
        Color baseTint = Color.Lerp(btnColor, Color.white, 0.1f);
        baseTint.a = 0.2f;

        // Custom Shader Material 
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", isWideAction ? 0.2f : 0.12f);
            glassMat.SetFloat("_EdgePadding", 0.04f);
            
            // We can't know accurate aspect ratio here easily without layout rebuild.
            // We'll set a default and maybe update it via script if needed.
            glassMat.SetFloat("_Aspect", isWideAction ? 4.0f : 1.4f); 
            
            glassMat.SetColor("_ColorA", new Color(btnColor.r, btnColor.g, btnColor.b, 0.15f));
            glassMat.SetColor("_ColorB", new Color(0f, 0.5f, 1f, 0.1f));
            glassMat.SetFloat("_GlassAlpha", 0.1f);
            
             bg.material = glassMat;
             bg.color = Color.white;
        }
        else
        {
            bg.color = baseTint;
        }
        
        // Interaction
        Button btn = btnHitObj.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(onClick);
        
        // Colors
        ColorBlock cb = btn.colors;
        cb.normalColor = baseTint;
        cb.highlightedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.5f);
        cb.pressedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.7f);
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
        
        // --- BORDER ---
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.offsetMin = Vector2.zero; borderRT.offsetMax = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.raycastTarget = false;
        
        Shader glowShader = Shader.Find("Custom/GlowingElementBorder");
        if (glowShader != null)
        {
            Material glowMat = new Material(glowShader);
            glowMat.SetFloat("_Aspect", isWideAction ? 4.0f : 1.4f);
            glowMat.SetFloat("_EdgePadding", 0.04f);
            glowMat.SetColor("_GlowColor", btnColor);
            
            // THINNER LOOK as requested
            glowMat.SetFloat("_BorderWidth", 0.02f); // Thin
            glowMat.SetFloat("_GlowWidth", 0.06f);   // Sharp
            glowMat.SetFloat("_GlowIntensity", 1.8f);
            glowMat.SetFloat("_CornerRadius", isWideAction ? 0.2f : 0.12f);
            
            glowMat.SetFloat("_PulseEnabled", 0f); // Static for cleaner look usually
             
            borderImg.material = glowMat;
            borderImg.sprite = GetPixelSprite();
            
            borderObj.AddComponent<VRButtonRipple>().Initialize(glowMat, borderImg);
        }
        
        // --- TEXT & ICON ---
        // Content Container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(visualRoot.transform, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero; cRT.anchorMax = Vector2.one;
        cRT.sizeDelta = Vector2.zero;
        
        // Icon (If Present)
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(content.transform, false);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            
            RectTransform iRT = iconObj.GetComponent<RectTransform>();
            // Centered above text
            iRT.anchorMin = new Vector2(0.35f, 0.45f);
            iRT.anchorMax = new Vector2(0.65f, 0.8f);
            iRT.offsetMin = Vector2.zero; iRT.offsetMax = Vector2.zero;
        }
        
        // Text
        // If Wide Button (Connect), text is center big
        // Else text is bottom small
        
        if (isWideAction)
        {
             GameObject tObj = CreateText(content.transform, label, Vector2.zero, 48, Color.white, true);
             RectTransform tRT = tObj.GetComponent<RectTransform>();
             tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
             tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        }
        else
        {
             GameObject tObj = CreateText(content.transform, label, Vector2.zero, 32, Color.white, false);
             RectTransform tRT = tObj.GetComponent<RectTransform>();
             tRT.anchorMin = new Vector2(0f, 0.1f); tRT.anchorMax = new Vector2(1f, 0.4f);
             tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        }

        // Dropdown Arrow (If requested - Image 2 style)
        if (showDropdown)
        {
             // Add small arrow indicator at bottom right
             // For now just a simple text "v" or similar if we lack sprite
             CreateText(content.transform, "▼", new Vector2(0,0), 20, new Color(1,1,1,0.5f)).GetComponent<RectTransform>().anchorMin = new Vector2(0.85f, 0.1f);
        }

        // Animation
        var anim = btnHitObj.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform;
        anim.popAmount = 0.05f;
    }


    
    GameObject CreateText(Transform parent, string content, Vector2 pos, int size, Color c, bool bold = false)
    {
        var go = new GameObject("TextTMP");
        go.transform.SetParent(parent, false);
        
        var txt = go.AddComponent<TextMeshProUGUI>();
        if (customFont != null) txt.font = customFont;
        
        txt.text = content;
        txt.fontSize = size;
        txt.color = c;
        txt.alignment = TextAlignmentOptions.Center;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.raycastTarget = false;
        
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.localScale = Vector3.one;
        rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f); 
        
        if (parent.name == "MenuCanvas")
            rt.sizeDelta = new Vector2(800, 150);
        else 
            rt.sizeDelta = new Vector2(400, 100);
        
        return go;
    }

    void OpenRemoteDesktop()
    {
        SwitchToRemoteMenu();
    }
}

public class VRButtonAnimation : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerClickHandler
{
    public Transform targetVisuals; // Target to animate
    public float popAmount = 0.1f;  // Set Default to 0.1

    private bool _isHovered = false;
    private float _currentPop = 0f;
    private float _currentValidScale = 1.0f;
    
    // Shader material reference for hover state
    private Material _glowMaterial;
    
    void Start()
    {
        // Try to find glow material in children
        var borderObj = targetVisuals?.Find("Border");
        if (borderObj != null)
        {
            var img = borderObj.GetComponent<Image>();
            if (img != null && img.material != null)
            {
                _glowMaterial = img.material;
            }
        }
    }

    void Update()
    {
        float targetZ = _isHovered ? -popAmount : 0f;
        _currentPop = Mathf.Lerp(_currentPop, targetZ, Time.unscaledDeltaTime * 10f);
        
        float targetScale = _isHovered ? 1.05f : 1.0f;
        _currentValidScale = Mathf.Lerp(_currentValidScale, targetScale, Time.unscaledDeltaTime * 10f);

        if (targetVisuals != null)
        {
            targetVisuals.localPosition = new Vector3(0, 0, _currentPop);
            targetVisuals.localScale = new Vector3(_currentValidScale, _currentValidScale, 1f);
        }
        
        // Update shader hover amount
        if (_glowMaterial != null)
        {
            float currentHover = _glowMaterial.GetFloat("_HoverAmount");
            float targetHover = _isHovered ? 1f : 0f;
            float newHover = Mathf.Lerp(currentHover, targetHover, Time.unscaledDeltaTime * 8f);
            _glowMaterial.SetFloat("_HoverAmount", newHover);
        }
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = true;
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = false;
    }
    
    public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData)
    {
        // Trigger ripple effect
        var ripple = GetComponentInChildren<VRButtonRipple>();
        if (ripple != null)
        {
            // Calculate click position in normalized coordinates
            RectTransform rt = targetVisuals?.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out localPoint);
                Vector2 normalizedPos = new Vector2(
                    (localPoint.x / rt.rect.width) + 0.5f,
                    (localPoint.y / rt.rect.height) + 0.5f
                );
                ripple.TriggerRipple(normalizedPos);
            }
            else
            {
                ripple.TriggerRipple(new Vector2(0.5f, 0.5f));
            }
        }
    }
}

public class VRButtonRipple : MonoBehaviour
{
    private Material _material;
    private Image _image;
    private bool _isAnimating = false;
    private float _rippleProgress = 0f;
    private float _rippleDuration = 0.5f;
    
    public void Initialize(Material mat, Image img)
    {
        _material = mat;
        _image = img;
    }
    
    public void TriggerRipple(Vector2 normalizedPosition)
    {
        if (_material == null) return;
        
        _material.SetVector("_RippleCenter", new Vector4(normalizedPosition.x, normalizedPosition.y, 0, 0));
        _rippleProgress = 0f;
        _isAnimating = true;
    }
    
    void Update()
    {
        if (!_isAnimating || _material == null) return;
        
        _rippleProgress += Time.unscaledDeltaTime / _rippleDuration;
        
        if (_rippleProgress >= 1f)
        {
            _rippleProgress = 0f;
            _isAnimating = false;
        }
        
        _material.SetFloat("_RippleProgress", _rippleProgress);
    }
}