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
            "icon_files", "icon_settings", "icon_quit", "icon_wifi", "icon_signal"
        };

        foreach (var name in iconNames)
        {
            try {
                string path = $"Assets/VR-Workspace/Resources/MainMenu/{name}.png";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    bool changed = false;
                    if (importer.textureType != TextureImporterType.Sprite)
                    {
                        importer.textureType = TextureImporterType.Sprite;
                        changed = true;
                    }
                    // Disable mipmaps for sharper UI icons
                    if (importer.mipmapEnabled)
                    {
                        importer.mipmapEnabled = false;
                        changed = true;
                    }
                    // Use uncompressed for best quality
                    if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                    {
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        changed = true;
                    }

                    if (changed) importer.SaveAndReimport();
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
        
        
        BuildMenuLayout(_menuFrame.ContentContainer, logicalWidth, contentHeight, 200f);
        
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
        
        // Exact logic from VRMainMenu_old.cs
        float marginPX = 40f; 
        float xMin = marginPX / w;
        float xMax = 1f - xMin;
        float yMin = marginPX / h;
        float yMax = 1f - (topMargin / h); 

        rt.anchorMin = new Vector2(xMin, yMin); 
        rt.anchorMax = new Vector2(xMax, yMax); 
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        GridLayoutGroup grid = _gridContainer.AddComponent<GridLayoutGroup>();
        
        float containerW = w * (xMax - xMin);
        float containerH = h * (yMax - yMin);
        
        int cols = 3;
        int rows = 2;
        
        float spacingX = 130f; // Increased by 30%
        float spacingY = 100f;
        float totalSpacingW = spacingX * (cols - 1);
        float totalSpacingH = spacingY * (rows - 1);

        float maxW = (containerW - totalSpacingW) / cols;
        float maxH = (containerH - totalSpacingH) / rows;

        float targetAspect = 1.32f; // Increased by 15% (1.15 * 1.15)
        float finalH = maxH;
        float finalW = finalH * targetAspect;

        if (finalW > maxW)
        {
            finalW = maxW;
            finalH = finalW / targetAspect;
        }

        grid.cellSize = new Vector2(finalW, finalH);
        grid.spacing = new Vector2(spacingX, spacingY);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3; 
        
        // Pass calculated size for Collider/Glow
        Vector2 btnSize = new Vector2(finalW, finalH);

        // --- ROW 1: APPS ---
        CreateGridButton("Remote Desktop", iconRemote, buttonColors[0], btnSize, () => OpenRemoteDesktop());
        CreateGridButton("Browser", iconBrowser, buttonColors[1], btnSize, () => Debug.Log("Browser"));
        CreateGridButton("Media", iconMedia, buttonColors[2], btnSize, () => Debug.Log("Media"));

        // --- ROW 2: TOOLS ---
        // Using "Files", "Resolution", "FPS" as per new Requirement (Image 2 style content)
        // But using "Size and Spacing" from Old Requirement.
        CreateGridButton("Files", iconFiles, buttonColors[3], btnSize, () => Debug.Log("Files"), true);
        CreateGridButton("Settings", iconSettings, buttonColors[4], btnSize, () => Debug.Log("Settings"), true);
        CreateGridButton("Quit", iconQuit, buttonColors[5], btnSize, () => Debug.Log("Quit"), true);
    }
    
    // Adapted CreateGridButton from VRMainMenu_old.cs but using NEW Visual Style (CreateFlexibleButton internals)
    // We mix them: Use the sizing/structure from Old, but Visuals from New.
    void CreateGridButton(string label, Sprite icon, Color btnColor, Vector2 size, UnityEngine.Events.UnityAction onClick, bool showDropdown = false)
    {
         // 1. Structural Wrapper (Grid Cell)
        GameObject wrapper = new GameObject("Btn_" + label);
        wrapper.transform.SetParent(_gridContainer.transform, false);
        // GridLayoutGroup controls this RT, but we add one for safety
        RectTransform wrapperRT = wrapper.AddComponent<RectTransform>();
        
        // 2. Reuse CreateFlexibleButton logic but we need to ensure it fills the cell
        // The CreateFlexibleButton assumes it is inside a LayoutElement. 
        // Here we are inside a GridLayoutGroup cell.
        
        // We can just call CreateFlexibleButton(wrapper...) 
        // AND we must ensure the Collider size matches 'size'.
        
        CreateFlexibleButton(wrapper, label, icon, btnColor, onClick, showDropdown, false);
        
        // Fix Collider Size (The CreateFlexibleButton sets 200,100 default)
        // We find the HitArea/BoxCollider and update it.
        Transform hitArea = wrapper.transform.Find("HitArea");
        if (hitArea)
        {
            BoxCollider col = hitArea.GetComponent<BoxCollider>();
            if (col) col.size = new Vector3(size.x, size.y, 0.1f);
        }
    }

    // Helper to generate the internal visual structure (Kept from New Implementation)
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
        
        // Collider - Default size (will be overridden by CreateGridButton)
        BoxCollider col = btnHitObj.AddComponent<BoxCollider>();
        col.size = new Vector3(200, 100, 0.1f); 
        
        // 2. Visual Root
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btnHitObj.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        
        // Increase expansion and padding further to ensure quad edges are invisible
        float expansion = 0.12f; 
        visRT.anchorMin = new Vector2(-expansion, -expansion);
        visRT.anchorMax = new Vector2(1f + expansion, 1f + expansion);
        visRT.offsetMin = Vector2.zero; visRT.offsetMax = Vector2.zero;
        
        // Background
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetPixelSprite();
        bg.type = Image.Type.Simple;
        bg.raycastTarget = false;
        
        Color baseTint = new Color(btnColor.r, btnColor.g, btnColor.b, 0.08f); 

        // Custom Shader Material 
        Shader glassShader = Shader.Find("Custom/GlassGradientBackground");
        if (glassShader != null)
        {
            Material glassMat = new Material(glassShader);
            glassMat.SetFloat("_CornerRadius", 0.12f); 
            glassMat.SetFloat("_EdgePadding", 0.12f); 
            glassMat.SetFloat("_Aspect", 1.15f); 
            
            glassMat.SetColor("_ColorA", new Color(btnColor.r, btnColor.g, btnColor.b, 0.12f)); 
            glassMat.SetColor("_ColorB", new Color(btnColor.r, btnColor.g, btnColor.b, 0.04f)); 
            glassMat.SetFloat("_GlassAlpha", 0.075f); 
            
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
        cb.normalColor = Color.white; 
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
            glowMat.SetFloat("_Aspect", 1.15f); 
            glowMat.SetFloat("_EdgePadding", 0.12f); 
            
            // TIGHTER AND SHARPER LOOK
            Color borderGlowCol = Color.Lerp(btnColor, Color.white, 0.75f);
            glowMat.SetColor("_GlowColor", borderGlowCol);
            
            glowMat.SetFloat("_BorderWidth", 0.005f); // Thinner line
            glowMat.SetFloat("_GlowWidth", 0.03f); // Tight glow
            glowMat.SetFloat("_GlowIntensity", 2.5f); // Balanced bloom
            glowMat.SetFloat("_CornerRadius", 0.12f); 
            glowMat.SetFloat("_PulseEnabled", 0f);
             
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
        
        // Icon
        if (icon != null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(content.transform, false);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.sprite = icon;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;
            
            // Tint icon with button color (leaned more towards white for higher clarity)
            iconImg.color = Color.Lerp(btnColor, Color.white, 0.9f);
            
            // Softened Whiter Glow Layer 1 (Sharp Inner halo)
            Color glowCol = Color.Lerp(btnColor, Color.white, 0.7f);
            glowCol.a = 0.4f; // Reduced intensity
            float s1 = 2f;
            
            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponent<Shadow>().effectDistance = new Vector2(s1, -s1);
            
            iconObj.AddComponent<Shadow>().effectColor = glowCol;
            iconObj.GetComponents<Shadow>()[1].effectDistance = new Vector2(-s1, s1);

            // Soft Bloom (Outer halo)
            Color bloomCol = Color.Lerp(btnColor, Color.white, 0.8f);
            bloomCol.a = 0.15f; // Very subtle
            float s2 = 5f;
            
            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[2].effectDistance = new Vector2(s2, -s2);

            iconObj.AddComponent<Shadow>().effectColor = bloomCol;
            iconObj.GetComponents<Shadow>()[3].effectDistance = new Vector2(-s2, s2);
            
            RectTransform iRT = iconObj.GetComponent<RectTransform>();
            // Centered above text
            iRT.anchorMin = new Vector2(0.35f, 0.42f);
            iRT.anchorMax = new Vector2(0.65f, 0.72f);
            iRT.offsetMin = Vector2.zero; iRT.offsetMax = Vector2.zero;
        }
        
        // Text
        GameObject tObj = CreateText(content.transform, label, Vector2.zero, 42, Color.white, true);
        RectTransform tRT = tObj.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0f, 0.18f); tRT.anchorMax = new Vector2(1f, 0.42f);
        tRT.offsetMin = Vector2.zero; tRT.offsetMax = Vector2.zero;
        
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