using UnityEngine;
using UnityEngine.UI;
using TMPro; 
using System.Collections.Generic; 
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VRMainMenu : MonoBehaviour
{
    [Header("Configuration")]
    public float panelWidth = 1.6f;
    public float panelHeight = 0.9f;
    public RemotePlayMainMenu remotePlayLogic; 

    [Header("Cyberpunk Visual Style")]
    // Glassmorphism: Dark semi-transparent tint + Blur simulation via opacity layering
    public Color glassColor = new Color(0.1f, 0.15f, 0.2f, 0.85f); 
    public Color panelBorderColor = new Color(0.0f, 0.8f, 1.0f, 0.8f); 
    
    // Cyberpunk Neon Palette
    public Color[] buttonColors = new Color[] {
        new Color(0.0f, 1.0f, 1.0f, 1.0f), // #1 Cyan Neon (Main)
        new Color(0.8f, 0.0f, 1.0f, 1.0f), // #2 Purple Neon
        new Color(0.0f, 0.6f, 1.0f, 1.0f), // #3 Electric Blue
        new Color(1.0f, 0.0f, 0.5f, 1.0f), // #4 Hot Pink
        new Color(0.0f, 1.0f, 0.5f, 1.0f), // #5 Matrix Green
        new Color(1.0f, 0.5f, 0.0f, 1.0f)  // #6 Neon Orange
    };
    
    public int fontSize = 32;

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
    private Sprite _roundedSprite;
    private Sprite _pixelSprite;
    
    // Cache separate border sprites by thickness
    private Dictionary<int, Sprite> _borderSprites = new Dictionary<int, Sprite>();

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
        LoadIcons();
        BuildInterface();
    }

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

    Sprite GetRoundedSprite()
    {
        if (_roundedSprite != null) return _roundedSprite;
        int size = 512; 
        int radius = 80; 
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCorner = (x < radius && y < radius) || (x > size - radius && y < radius) ||
                                (x < radius && y > size - radius) || (x > size - radius && y > size - radius);

                if (inCorner)
                {
                    float cx = (x < size / 2) ? radius : size - radius - 1;
                    float cy = (y < size / 2) ? radius : size - radius - 1;
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    
                    float alpha = Mathf.Clamp01((radius + 0.5f) - d);
                    colors[y * size + x] = new Color(1, 1, 1, alpha);
                }
                else
                {
                    colors[y * size + x] = Color.white;
                }
            }
        }
        
        tex.SetPixels(colors);
        tex.Apply();
        _roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return _roundedSprite;
    }
    
    Sprite GetBorderSprite(int thickness)
    {
        if (_borderSprites.ContainsKey(thickness) && _borderSprites[thickness] != null) 
            return _borderSprites[thickness];
            
        int size = 512;
        int radius = 80;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color[] colors = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inCorner = (x < radius && y < radius) || (x > size - radius && y < radius) ||
                                (x < radius && y > size - radius) || (x > size - radius && y > size - radius);

                float alpha = 0f;

                if (inCorner)
                {
                    float cx = (x < size / 2) ? radius : size - radius - 1;
                    float cy = (y < size / 2) ? radius : size - radius - 1;
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    
                    float outerAlpha = Mathf.Clamp01((radius + 0.5f) - d);
                    float innerEdge = radius - thickness;
                    float innerAlpha = Mathf.Clamp01(d - (innerEdge - 0.5f));
                    
                    alpha = outerAlpha * innerAlpha;
                }
                else
                {
                    float dx = Mathf.Min(x, size - 1 - x);
                    float dy = Mathf.Min(y, size - 1 - y);
                    float minDist = Mathf.Min(dx, dy); 
                    
                    if (minDist < thickness + 1)
                    {
                         float innerAlpha = Mathf.Clamp01((thickness + 0.5f) - minDist);
                         alpha = innerAlpha;
                    }
                }
                colors[y * size + x] = new Color(1, 1, 1, alpha);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        _borderSprites[thickness] = s;
        return s;
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

    void BuildInterface()
    {
        Debug.Log("[VRMainMenu] Building Cyberpunk Interface V24 BoxCollider Added...");

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
        
        CreateGlassPanel(canvasGO.transform, logicalWidth, logicalHeight);
        
        // No Title

        CreateUniformGrid(canvasGO.transform, logicalWidth, logicalHeight);
        
        Canvas.ForceUpdateCanvases();
        var fitter = _gridContainer.GetComponent<GridLayoutGroup>();
        if(fitter) {
            fitter.CalculateLayoutInputHorizontal();
            fitter.CalculateLayoutInputVertical();
            fitter.SetLayoutHorizontal();
            fitter.SetLayoutVertical();
        }

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
    
    void CreateGlassPanel(Transform parent, float w, float h)
    {
        GameObject bgObj = new GameObject("GlassBackground");
        bgObj.transform.SetParent(parent, false);
        Image img = bgObj.AddComponent<Image>();
        
        img.type = Image.Type.Sliced;
        img.sprite = GetRoundedSprite();
        
        // Apply Custom Blur Shader
        Shader blurShader = Shader.Find("Custom/UIBlurBackground");
        if (blurShader != null)
        {
            Material blurMat = new Material(blurShader);
            blurMat.SetFloat("_Radius", 4.0f); 
            img.material = blurMat;
            img.color = glassColor; 
        }
        else
        {
            img.color = glassColor;
        }
        
        RectTransform rt = bgObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; 
        rt.sizeDelta = Vector2.zero; rt.localScale = Vector3.one;
        rt.localPosition = Vector3.zero;
        rt.SetAsFirstSibling();
        
        // ETHEREAL DEPTH BORDER
        GameObject borderObj = new GameObject("PanelBorder_Core");
        borderObj.transform.SetParent(bgObj.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.sizeDelta = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetBorderSprite(12); 
        borderImg.type = Image.Type.Sliced;
        borderImg.color = new Color(0.6f, 0.9f, 1.0f, 0.9f); 
        borderImg.raycastTarget = false;

        GameObject depthObj = new GameObject("PanelBorder_Depth");
        depthObj.transform.SetParent(bgObj.transform, false);
        depthObj.transform.SetAsFirstSibling();
        RectTransform depthRT = depthObj.AddComponent<RectTransform>();
        depthRT.anchorMin = Vector2.zero; depthRT.anchorMax = Vector2.one;
        depthRT.offsetMin = new Vector2(-6, -6); 
        depthRT.offsetMax = new Vector2(6, 6);
        
        Image depthImg = depthObj.AddComponent<Image>();
        depthImg.sprite = GetBorderSprite(24); 
        depthImg.type = Image.Type.Sliced;
        depthImg.color = new Color(0.0f, 0.5f, 1.0f, 0.15f); 
        depthImg.raycastTarget = false;
        
        var coreGlow = borderObj.AddComponent<UnityEngine.UI.Shadow>();
        coreGlow.effectColor = new Color(0f, 0.8f, 1f, 0.6f);
        coreGlow.effectDistance = new Vector2(0, 0); 
        
        var depthGlow = depthObj.AddComponent<UnityEngine.UI.Shadow>();
        depthGlow.effectColor = new Color(0f, 0.5f, 1f, 0.5f);
        depthGlow.effectDistance = new Vector2(0, -4);

        CreateFloatingDataEffects(bgObj.transform, w, h);
    }

    void CreateFloatingDataEffects(Transform parent, float w, float h)
    {
        GameObject fxContainer = new GameObject("FX_DataStream");
        fxContainer.transform.SetParent(parent, false);
        RectTransform rt = fxContainer.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        
        fxContainer.AddComponent<RectMask2D>();

        int particleCount = 20;
        for (int i = 0; i < particleCount; i++)
        {
            GameObject p = new GameObject($"Bit_{i}");
            p.transform.SetParent(fxContainer.transform, false);
            
            Image pImg = p.AddComponent<Image>();
            pImg.sprite = GetPixelSprite(); 
            
            bool cyanOrPurple = Random.value > 0.5f;
            Color baseCol = cyanOrPurple ? Color.cyan : new Color(0.8f, 0f, 1f); 
            pImg.color = new Color(baseCol.r, baseCol.g, baseCol.b, Random.Range(0.1f, 0.4f));

            RectTransform pRT = p.GetComponent<RectTransform>();
            float size = Random.Range(10f, 60f);
            pRT.sizeDelta = new Vector2(size, size * Random.Range(0.2f, 1.0f)); 
            
            float startX = Random.Range(-w/2f, w/2f);
            float startY = Random.Range(-h/2f, h/2f);
            pRT.anchoredPosition = new Vector2(startX, startY);

            var anim = p.AddComponent<FloatingDataAnim>();
            anim.speed = Random.Range(10f, 40f);
            anim.range = new Vector2(w, h);
        }
    }

    void CreateUniformGrid(Transform parent, float w, float h)
    {
        _gridContainer = new GameObject("ButtonGrid");
        _gridContainer.transform.SetParent(parent, false);

        RectTransform rt = _gridContainer.AddComponent<RectTransform>();
        
        float marginPX = 100f; 
        float xMin = marginPX / w;
        float xMax = 1f - xMin;
        float yMin = marginPX / h;
        float yMax = 1f - yMin;

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
        
        float spacingPX = 100f;
        float totalSpacingW = spacingPX * (cols - 1);
        float totalSpacingH = spacingPX * (rows - 1);

        float maxW = (containerW - totalSpacingW) / cols;
        float maxH = (containerH - totalSpacingH) / rows;

        float targetAspect = 1.15f; 
        float finalH = maxH;
        float finalW = finalH * targetAspect;

        if (finalW > maxW)
        {
            finalW = maxW;
            finalH = finalW / targetAspect;
        }

        grid.cellSize = new Vector2(finalW, finalH);
        grid.spacing = new Vector2(spacingPX, spacingPX);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3; 
        
        // Pass calculated size
        Vector2 btnSize = new Vector2(finalW, finalH);

        CreateGridButton("Remote Desktop", iconRemote, buttonColors[0], btnSize, () => OpenRemoteDesktop());
        CreateGridButton("Browser", iconBrowser, buttonColors[1], btnSize, () => Debug.Log("Browser"));
        CreateGridButton("Media", iconMedia, buttonColors[2], btnSize, () => Debug.Log("Media"));
        CreateGridButton("Files", iconFiles, buttonColors[3], btnSize, () => Debug.Log("Files"));
        CreateGridButton("Settings", iconSettings, buttonColors[4], btnSize, () => Debug.Log("Settings"));
        CreateGridButton("Quit", iconQuit, buttonColors[5], btnSize, () => Application.Quit());
    }

    void CreateGridButton(string label, Sprite icon, Color btnColor, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        // 1. Structural Wrapper (Layout Cell)
        GameObject wrapper = new GameObject("Cell_" + label);
        wrapper.transform.SetParent(_gridContainer.transform, false);
        RectTransform wrt = wrapper.AddComponent<RectTransform>();
        wrt.localScale = Vector3.one;
        
        // 2. HIT AREA (Static Interaction Layer)
        // This object stays still to catch raycasts stably
        GameObject btnHitObj = new GameObject("Btn_HitArea");
        btnHitObj.transform.SetParent(wrapper.transform, false);
        RectTransform hitRT = btnHitObj.AddComponent<RectTransform>();
        hitRT.anchorMin = Vector2.zero; hitRT.anchorMax = Vector2.one;
        hitRT.sizeDelta = Vector2.zero;
        
        Image hitImg = btnHitObj.AddComponent<Image>();
        hitImg.color = Color.clear; 
        
        // --- ADD BOX COLLIDER FOR GAZE RAYCAST ---
        BoxCollider col = btnHitObj.AddComponent<BoxCollider>();
        // Fix Z size: Canvas Z scale is 1, so we use small value here (0.1f = 10cm)
        col.size = new Vector3(size.x, size.y, 0.1f); 
        // -----------------------------------------
        
        // 3. VISUAL ROOT (Animated Layer)
        GameObject visualRoot = new GameObject("Visuals");
        visualRoot.transform.SetParent(btnHitObj.transform, false);
        RectTransform visRT = visualRoot.AddComponent<RectTransform>();
        visRT.anchorMin = Vector2.zero; visRT.anchorMax = Vector2.one;
        visRT.sizeDelta = Vector2.zero;

        // Background (on Visual Root)
        Image bg = visualRoot.AddComponent<Image>();
        bg.sprite = GetRoundedSprite();
        bg.type = Image.Type.Sliced;
        bg.raycastTarget = false; 
        
        Color baseTint = Color.Lerp(btnColor, Color.white, 0.2f);
        baseTint.a = 0.4f;

        // Apply Blur Shader if found
        Shader blurShader = Shader.Find("Custom/UIBlurBackground");
        if (blurShader != null)
        {
            Material blurMat = new Material(blurShader);
            blurMat.SetFloat("_Radius", 3.0f); 
            bg.material = blurMat;
            bg.color = baseTint;
        }
        else
        {
             bg.color = baseTint; 
        }

        // Button Component (on Hit Area)
        Button btn = btnHitObj.AddComponent<Button>();
        btn.targetGraphic = bg; 
        btn.onClick.AddListener(onClick);
        
        ColorBlock cb = btn.colors;
        cb.normalColor = baseTint;
        cb.highlightedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.6f);
        cb.pressedColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.8f);
        cb.colorMultiplier = 1f; 
        cb.fadeDuration = 0.1f;
        btn.colors = cb;
        
        // BORDER OBJECT
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(visualRoot.transform, false);
        RectTransform borderRT = borderObj.AddComponent<RectTransform>();
        borderRT.anchorMin = Vector2.zero; borderRT.anchorMax = Vector2.one;
        borderRT.sizeDelta = Vector2.zero;
        
        Image borderImg = borderObj.AddComponent<Image>();
        borderImg.sprite = GetBorderSprite(4); 
        borderImg.type = Image.Type.Sliced;
        borderImg.color = Color.Lerp(btnColor, Color.white, 0.8f); 
        borderImg.raycastTarget = false;
        
        var glow = borderObj.AddComponent<UnityEngine.UI.Shadow>();
        glow.effectColor = new Color(btnColor.r, btnColor.g, btnColor.b, 1.0f); 
        glow.effectDistance = new Vector2(0, -2);
        
        var glow2 = borderObj.AddComponent<UnityEngine.UI.Shadow>();
        glow2.effectColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.8f); 
        glow2.effectDistance = new Vector2(0, 3);
        
        var glow3 = borderObj.AddComponent<UnityEngine.UI.Shadow>();
        glow3.effectColor = new Color(btnColor.r, btnColor.g, btnColor.b, 0.4f); 
        glow3.effectDistance = new Vector2(3, -3);

        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(visualRoot.transform, false);
        RectTransform cRT = content.AddComponent<RectTransform>();
        cRT.anchorMin = Vector2.zero; cRT.anchorMax = Vector2.one;
        cRT.sizeDelta = Vector2.zero;

        // Icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(content.transform, false);
        Image iconImg = iconObj.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.color = Color.white; 
        iconImg.raycastTarget = false;
        
        var iconGlow = iconObj.AddComponent<UnityEngine.UI.Shadow>();
        iconGlow.effectColor = btnColor;
        iconGlow.effectDistance = new Vector2(2, -2);

        RectTransform iconRT = iconObj.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.32f, 0.40f); 
        iconRT.anchorMax = new Vector2(0.68f, 0.75f);
        iconRT.offsetMin = Vector2.zero; iconRT.offsetMax = Vector2.zero;
        iconImg.preserveAspect = true;

        // Text
        GameObject txtObj = CreateText(content.transform, label, Vector2.zero, fontSize, Color.white, true); 
        
        var txtGlow = txtObj.AddComponent<UnityEngine.UI.Shadow>();
        txtGlow.effectColor = btnColor;
        txtGlow.effectDistance = new Vector2(1.5f, -1.5f);

        var txtRT = txtObj.GetComponent<RectTransform>();
        txtRT.anchorMin = new Vector2(0, 0.12f);
        txtRT.anchorMax = new Vector2(1, 0.35f); 

        // Animation Logic
        var anim = btnHitObj.AddComponent<VRButtonAnimation>();
        anim.targetVisuals = visualRoot.transform; 
        anim.popAmount = 0.1f; 
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
        if (_canvas) _canvas.gameObject.SetActive(false);
        
        if (remotePlayLogic) remotePlayLogic.gameObject.SetActive(true);
        else 
        {
            var rp = FindObjectOfType<RemotePlayMainMenu>(true);
            if (rp) rp.gameObject.SetActive(true);
        }
    }
}

public class VRButtonAnimation : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public Transform targetVisuals; // Target to animate
    public float popAmount = 0.1f;  // Set Default to 0.1

    private bool _isHovered = false;
    private float _currentPop = 0f;
    private float _currentValidScale = 1.0f;

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
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = true;
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData)
    {
        _isHovered = false;
    }
}

public class FloatingDataAnim : MonoBehaviour
{
    public float speed;
    public Vector2 range; 
    private RectTransform _rt;
    private Vector2 _dir;

    void Start()
    {
        _rt = GetComponent<RectTransform>();
        _dir = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.8f)).normalized; 
        if (Random.value > 0.5f) _dir.y *= -1; 
    }

    void Update()
    {
        if (_rt == null) return;
        _rt.anchoredPosition += _dir * speed * Time.deltaTime;

        float halfW = range.x / 2f + 50f;
        float halfH = range.y / 2f + 50f;

        if (_rt.anchoredPosition.y > halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), -halfH);
        else if (_rt.anchoredPosition.y < -halfH) _rt.anchoredPosition = new Vector2(Random.Range(-halfW, halfW), halfH);
        
        if (_rt.anchoredPosition.x > halfW) _rt.anchoredPosition = new Vector2(-halfW, Random.Range(-halfH, halfH));
        else if (_rt.anchoredPosition.x < -halfW) _rt.anchoredPosition = new Vector2(halfW, Random.Range(-halfH, halfH));
    }
}
